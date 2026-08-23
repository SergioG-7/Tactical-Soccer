using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    // Detecta cuando el balón entra en la portería y dispara el evento de gol.
    [RequireComponent(typeof(Collider))]
    public class GoalDetector : MonoBehaviour
    {
        [SerializeField] private int teamToScore;

        // Asigna qué equipo marca al entrar el balón en esta portería.
        public void ConfigureTeam(int scoringTeam)
        {
            teamToScore = scoringTeam;
        }

        // Comprueba si el balón ha entrado limpiamente en la portería y, si es así, dispara el gol.
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Ball"))
            {
                return;
            }

            if (!other.TryGetComponent(out BallController ball))
            {
                return;
            }

            // Solo cuenta si el balón está suelto, no si lo tiene agarrado el portero.
            if (!ball.IsFree)
            {
                return;
            }

            // Evita contar el mismo gol varias veces mientras se celebra.
            if (Core.MatchManager.IsGoalBeingCelebrated)
            {
                return;
            }

            // Frena el balón para que no atraviese la red por la inercia del disparo.
            ball.DampenIntoNet();

            Core.TacticalEvents.OnGoalScored?.Invoke(teamToScore);

            if (Core.MatchManager.Instance != null)
            {
                Core.MatchManager.Instance.CelebrateGoal();
                return;
            }

            // Sin MatchManager, se reinicia directamente sin celebración.
            ball.ResetToKickoff();
        }
    }
}
