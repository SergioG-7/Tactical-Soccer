using System.Collections.Generic;
using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    // Controla a todo un equipo, usando el mismo sistema de rutas que dibuja el jugador humano.
    public class EnemyAIController : MonoBehaviour
    {
        [SerializeField] private TeamId controlledTeam = TeamId.Red;

        [Tooltip("Tiempo base entre decisiones. Escala con la dificultad elegida.")]
        [SerializeField] private float thinkInterval = 1f;

        [Tooltip("Punto objetivo de ataque. Situado tras la línea de meta para asegurar que el portador la cruce.")]
        [SerializeField] private Vector3 targetGoalPosition = new Vector3(0f, 0f, -24.5f);

        [Tooltip("Centro geométrico de la portería rival hacia donde se apunta el disparo.")]
        [SerializeField] private Vector3 shotTargetPosition = new Vector3(0f, 0f, -23.5f);

        [Tooltip("Distancia desde la portería a la que el portador decide disparar en vez de seguir corriendo.")]
        [SerializeField] private float shootingRange = 15f;

        [Tooltip("Probabilidad de realizar un pase. Se mantiene baja para forzar a la IA a conducir el balón también.")]
        [Range(0f, 1f)]
        [SerializeField] private float passChance = 0.3f;

        [Tooltip("Radio de distancia para considerar que un compañero está marcado por un rival.")]
        [SerializeField] private float markedRadius = 3.5f;

        [Tooltip("Distancia mínima de avance necesaria para que la IA considere que vale la pena el pase.")]
        [SerializeField] private float minimumPassAdvance = 3f;

        [Tooltip("Distancia máxima permitida para realizar un pase.")]
        [SerializeField] private float maximumPassDistance = 18f;

        [Tooltip("Distancia extra más allá del objetivo al presionar, para asegurar el contacto en movimiento.")]
        [SerializeField] private float pressOvershoot = 1.5f;

        private readonly List<PlayerBallHandler> squad = new List<PlayerBallHandler>();
        private BallController ball;
        private float thinkTimer;

        // Tiempo actual entre decisiones, según la dificultad elegida.
        private float CurrentThinkInterval
        {
            get
            {
                float scale = Core.MatchManager.Instance != null
                    ? Core.MatchManager.Instance.AiThinkIntervalScale
                    : 1f;

                return thinkInterval * scale;
            }
        }

        // Busca el balón y prepara la plantilla de este equipo.
        private void Start()
        {
            ball = FindAnyObjectByType<BallController>();
            CacheSquad();
        }

        // Cuenta el tiempo entre decisiones y llama a Think cuando toca, salvo en duelos o reinicios.
        private void Update()
        {
            if (ball == null || squad.Count == 0)
            {
                return;
            }

            if (ClashManager.IsClashActive)
            {
                return;
            }

            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                return;
            }

            thinkTimer += Time.deltaTime;

            if (thinkTimer < CurrentThinkInterval)
            {
                return;
            }

            thinkTimer = 0f;
            Think();
        }

        // Guarda la lista de jugadores de este equipo (sin contar al portero).
        private void CacheSquad()
        {
            squad.Clear();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != controlledTeam)
                {
                    continue;
                }

                if (member.isGoalkeeper)
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerBallHandler handler))
                {
                    squad.Add(handler);
                }
            }
        }

        // Decide la siguiente acción: rematar, pasar, avanzar con el balón, presionar o ir a por el balón.
        private void Think()
        {
            PlayerBallHandler carrier = FindCarrier();

            if (carrier != null)
            {
                if (IsInShootingRange(carrier))
                {
                    Shoot(carrier);
                    return;
                }

                if (Random.value < passChance && TryPass(carrier))
                {
                    return;
                }

                SendTo(carrier, targetGoalPosition);
                return;
            }

            TeamMember opposingCarrier = FindOpposingCarrier();

            if (opposingCarrier != null)
            {
                Press(opposingCarrier);
                return;
            }

            PlayerBallHandler chaser = FindClosestToBall();

            if (chaser != null)
            {
                SendTo(chaser, ball.transform.position);
            }
        }

        // Envía al jugador más cercano a presionar al portador rival, apuntando un poco más allá de él.
        private void Press(TeamMember carrier)
        {
            Vector3 carrierPosition = carrier.transform.position;
            PlayerBallHandler presser = FindClosestTo(carrierPosition);

            if (presser == null)
            {
                return;
            }

            Vector3 approach = carrierPosition - presser.transform.position;
            approach.y = 0f;

            Vector3 target = approach.sqrMagnitude > 0.0001f
                ? carrierPosition + (approach.normalized * pressOvershoot)
                : carrierPosition;

            Debug.Log($"[IA] {presser.name} presiona a {carrier.name} " +
                      $"({approach.magnitude:F1} u).");

            SendTo(presser, target);
        }

        // Busca quién del equipo contrario lleva el balón, si alguien lo lleva.
        private TeamMember FindOpposingCarrier()
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == controlledTeam || !member.isStarter)
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerBallHandler handler) && handler.HasBall)
                {
                    return member;
                }
            }

            return null;
        }

        // Cierto si el portador está lo bastante cerca de la portería para rematar.
        private bool IsInShootingRange(PlayerBallHandler carrier)
        {
            Vector3 toGoal = shotTargetPosition - carrier.transform.position;
            toGoal.y = 0f;

            return toGoal.magnitude < shootingRange;
        }

        // Cancela la ruta del jugador y le hace rematar a puerta.
        private void Shoot(PlayerBallHandler carrier)
        {
            if (carrier.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
            }

            Debug.Log($"[IA] {carrier.name} remata a puerta desde " +
                      $"{Vector3.Distance(carrier.transform.position, shotTargetPosition):F1} u.");

            carrier.InitiateShot(shotTargetPosition);
        }

        // Busca a un compañero libre y adelantado al que pasar; devuelve false si no hay ninguno.
        private bool TryPass(PlayerBallHandler carrier)
        {
            PlayerBallHandler target = FindPassTarget(carrier);

            if (target == null)
            {
                return false;
            }

            if (carrier.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
            }

            Debug.Log($"[IA] {carrier.name} pasa a {target.name} " +
                      $"({Vector3.Distance(carrier.transform.position, target.transform.position):F1} u).");

            carrier.PassTo(target.transform.position);

            return true;
        }

        // Elige al mejor compañero al que pasar: el más adelantado, libre y a distancia de pase.
        private PlayerBallHandler FindPassTarget(PlayerBallHandler carrier)
        {
            float attackDirection = Mathf.Sign(shotTargetPosition.z);
            Vector3 carrierPosition = carrier.transform.position;

            PlayerBallHandler best = null;
            float bestAdvance = minimumPassAdvance;

            foreach (PlayerBallHandler mate in squad)
            {
                if (mate == null || mate == carrier || !mate.IsOnPitch)
                {
                    continue;
                }

                Vector3 matePosition = mate.transform.position;

                float advance = (matePosition.z - carrierPosition.z) * attackDirection;

                if (advance <= bestAdvance)
                {
                    continue;
                }

                Vector3 toMate = matePosition - carrierPosition;
                toMate.y = 0f;

                if (toMate.magnitude > maximumPassDistance)
                {
                    continue;
                }

                if (IsMarked(matePosition))
                {
                    continue;
                }

                bestAdvance = advance;
                best = mate;
            }

            return best;
        }

        // Cierto si hay algún rival lo bastante cerca de esa posición para disputar el balón.
        private bool IsMarked(Vector3 position)
        {
            float markedSqr = markedRadius * markedRadius;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == controlledTeam || !member.isStarter)
                {
                    continue;
                }

                if ((member.transform.position - position).sqrMagnitude <= markedSqr)
                {
                    return true;
                }
            }

            return false;
        }

        // Busca quién de este equipo lleva el balón.
        private PlayerBallHandler FindCarrier()
        {
            foreach (PlayerBallHandler handler in squad)
            {
                if (handler != null && handler.IsOnPitch && handler.HasBall)
                {
                    return handler;
                }
            }

            return null;
        }

        // Busca al jugador de este equipo más cercano al balón.
        private PlayerBallHandler FindClosestToBall()
        {
            return FindClosestTo(ball.transform.position);
        }

        // Busca al jugador de este equipo más cercano a un punto dado.
        private PlayerBallHandler FindClosestTo(Vector3 point)
        {
            PlayerBallHandler closest = null;
            float closestSqrDistance = float.MaxValue;
            Vector3 ballPosition = point;

            foreach (PlayerBallHandler handler in squad)
            {
                if (handler == null || !handler.IsOnPitch)
                {
                    continue;
                }

                float sqrDistance = (handler.transform.position - ballPosition).sqrMagnitude;

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = handler;
                }
            }

            return closest;
        }

        // Traza una ruta directa de un jugador hasta un destino.
        private void SendTo(PlayerBallHandler handler, Vector3 destination)
        {
            if (!handler.TryGetComponent(out PlayerRoute route))
            {
                return;
            }

            route.BeginRoute();
            route.AddRoutePoint(Core.PitchBounds.ClampPlayer(destination));
            route.EndRoute();
        }
    }
}
