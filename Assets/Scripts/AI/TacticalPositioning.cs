using System.Collections.Generic;
using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    // Mueve al jugador cuando no tiene nada más que hacer: va a por un balón suelto cercano o mantiene su puesto de formación.
    [RequireComponent(typeof(PlayerRoute))]
    public class TacticalPositioning : MonoBehaviour
    {
        [Tooltip("Grado de desplazamiento del centrocampista hacia la posición del balón.")]
        [SerializeField] private float ballInfluence = 0.3f;

        [Tooltip("Influencia del balón en la posición del delantero (mayor que en el medio para apoyar el ataque).")]
        [SerializeField] private float forwardBallInfluence = 0.45f;

        [Tooltip("Distancia que adelanta el delantero respecto a su posición base para dar salida al equipo.")]
        [SerializeField] private float forwardPush = 6f;

        [Tooltip("Influencia del balón en los defensas. Baja para mantener la línea defensiva.")]
        [SerializeField] private float defenderBallInfluence = 0.15f;

        [Tooltip("Límite máximo que un defensa puede cruzar hacia campo rival.")]
        [SerializeField] private float defenderMaxAdvance = 2f;

        [Tooltip("Desviación máxima permitida respecto a la posición base de la formación.")]
        [SerializeField] private float driftRange = 1.5f;

        [Tooltip("Velocidad a la que se calcula el desplazamiento para evitar vibraciones en la animación.")]
        [SerializeField] private float driftSpeed = 0.5f;

        [Tooltip("Velocidad de movimiento al recolocarse en la formación.")]
        [SerializeField] private float repositionSpeed = 2f;

        [Tooltip("Radio de detección para acudir a disputar un balón suelto.")]
        [SerializeField] private float chaseRadius = 12f;

        [Tooltip("Velocidad del jugador al esprintar hacia un balón suelto.")]
        [SerializeField] private float chaseSpeed = 3f;

        private TeamMember member;
        private PlayerRoute route;
        private PlayerBallHandler handler;

        private Vector3 baseFormationPos;

        // Puesto de formación de este jugador.
        public Vector3 FormationSlot => baseFormationPos;

        // Compañeros de campo de este equipo, para comparar distancias al ir a por el balón.
        private readonly List<TeamMember> teamMates = new List<TeamMember>();

        // Offset individual en el ruido de deriva, para que no todos los jugadores se muevan igual.
        private float noiseSeed;

        private const float NoiseSeedX = 7.31f;
        private const float NoiseSeedZ = 2.17f;
        private const float NoiseSeedBase = 500f;

        // Cachea componentes y desactiva el script para los porteros, que se mueven con su propia lógica.
        private void Awake()
        {
            member = GetComponent<TeamMember>();
            route = GetComponent<PlayerRoute>();
            handler = GetComponent<PlayerBallHandler>();

            SetFormationSlot(transform.position);

            if (member != null && member.isGoalkeeper)
            {
                enabled = false;
            }
        }

        // Cambia el puesto de formación y recalcula la semilla de ruido para este jugador.
        public void SetFormationSlot(Vector3 slot)
        {
            baseFormationPos = slot;

            noiseSeed = (baseFormationPos.x * NoiseSeedX)
                + (baseFormationPos.z * NoiseSeedZ)
                + NoiseSeedBase;
        }

        private void Start()
        {
            CacheTeamMates();
        }

        // Persigue el balón suelto más cercano o mantiene el puesto de formación.
        private void Update()
        {
            if (!ShouldReposition())
            {
                return;
            }

            bool chasing = ShouldChaseLooseBall();

            Vector3 target = chasing
                ? Core.PitchBounds.ClampPlayer(BallController.Instance.transform.position)
                : CalculateFormationPosition();

            target.y = transform.position.y;

            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                (chasing ? chaseSpeed : repositionSpeed) * Time.deltaTime);
        }

        // Comprueba si el jugador debe moverse ahora mismo o dejar el control a otro sistema.
        private bool ShouldReposition()
        {
            if (member != null && member.isGoalkeeper)
            {
                return false;
            }

            if (member != null && !member.isStarter)
            {
                return false;
            }

            if (ClashManager.IsClashActive)
            {
                return false;
            }

            if (handler != null && handler.HasBall)
            {
                return false;
            }

            if (route != null && (route.IsFollowingRoute || route.IsStunned))
            {
                return false;
            }

            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                return false;
            }

            return BallController.Instance != null;
        }

        // Indica si este jugador es el más cercano de su equipo a un balón suelto y está a distancia de ir a por él.
        private bool ShouldChaseLooseBall()
        {
            BallController ball = BallController.Instance;

            if (!ball.IsFree)
            {
                return false;
            }

            Vector3 ballPosition = ball.transform.position;
            float ownDistance = FlatDistance(transform.position, ballPosition);

            if (ownDistance > chaseRadius)
            {
                return false;
            }

            foreach (TeamMember mate in teamMates)
            {
                if (mate == null || !mate.isStarter)
                {
                    continue;
                }

                if (FlatDistance(mate.transform.position, ballPosition) < ownDistance)
                {
                    return false;
                }
            }

            return true;
        }

        // Distancia entre dos puntos ignorando la altura.
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        // Reconstruye la lista de compañeros de campo a partir de la plantilla actual.
        public void CacheTeamMates()
        {
            teamMates.Clear();

            if (member == null)
            {
                return;
            }

            foreach (TeamMember other in FindObjectsByType<TeamMember>())
            {
                if (other == member || other.team != member.team || other.isGoalkeeper)
                {
                    continue;
                }

                teamMates.Add(other);
            }
        }

        // Calcula el punto donde el jugador debe estar según su formación, deriva y posición del balón.
        private Vector3 CalculateFormationPosition()
        {
            float driftX = SampleDrift(0.13f);
            float driftZ = SampleDrift(4.71f);

            PlayerRole role = member != null ? member.role : PlayerRole.Midfielder;

            // Dirección de ataque de este equipo: Azul hacia el norte, Rojo hacia el sur.
            float attackDirection = member != null && member.team == TeamId.Red ? -1f : 1f;

            float zShift = BallController.Instance.transform.position.z * ResolveBallInfluence(role);

            if (role == PlayerRole.Forward)
            {
                zShift += attackDirection * forwardPush;
            }

            Vector3 target = baseFormationPos + new Vector3(driftX, 0f, zShift + driftZ);

            if (role == PlayerRole.Defender)
            {
                // Distancia avanzada hacia la portería rival, para aplicar el mismo límite a ambos equipos.
                float advance = target.z * attackDirection;

                if (advance > defenderMaxAdvance)
                {
                    target.z = attackDirection * defenderMaxAdvance;
                }
            }

            return Core.PitchBounds.ClampPlayer(target);
        }

        // Devuelve cuánto influye la posición del balón según el rol del jugador.
        private float ResolveBallInfluence(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return forwardBallInfluence;
                case PlayerRole.Defender: return defenderBallInfluence;
                default: return ballInfluence;
            }
        }

        // Calcula el desplazamiento de deriva en un canal de ruido, centrado en cero.
        private float SampleDrift(float channel)
        {
            float noise = Mathf.PerlinNoise((Time.time * driftSpeed) + noiseSeed, channel);

            return (noise - 0.5f) * 2f * driftRange;
        }
    }
}
