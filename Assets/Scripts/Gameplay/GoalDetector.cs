using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    /// <summary>
    /// Trigger volume sitting between the goalposts. Announces the goal and the
    /// restart, then puts the ball back on the centre spot. Owns no score of
    /// its own; counting is somebody else's job.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GoalDetector : MonoBehaviour
    {
        [Tooltip("Id of the team that scores in this goal: 0 = Blue, 1 = Red.")]
        [SerializeField] private int teamToScore;

        public void ConfigureTeam(int scoringTeam)
        {
            teamToScore = scoringTeam;
        }

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

            // Only a loose ball can score. A held ball is glued to its owner's
            // socket and repositioned every LateUpdate, so a keeper catching it
            // on the goal line re-entered this trigger frame after frame and
            // racked up a goal each time.
            if (!ball.IsFree)
            {
                return;
            }

            // The ball now STAYS in the net for a couple of seconds instead of
            // vanishing to the centre spot on the scoring frame, which means it
            // is free to roll out of this trigger and back into it — and each
            // re-entry would count as another goal.
            if (Core.MatchManager.IsGoalBeingCelebrated)
            {
                return;
            }

            // Killed on the way in, before anything else runs. A shot struck at
            // full force is still carrying that force when it crosses the line,
            // and the netting is thin scenery — the ball would punch straight
            // through it and be seen sailing away behind the goal for the whole
            // celebration, which reads as a miss rather than as a goal.
            ball.DampenIntoNet();

            Core.TacticalEvents.OnGoalScored?.Invoke(teamToScore);

            if (Core.MatchManager.Instance != null)
            {
                // Leaves the ball where it is, shows the goal, and resets from
                // the centre spot once it has been seen.
                Core.MatchManager.Instance.CelebrateGoal();
                return;
            }

            // No match manager to run the celebration: fall back to the old
            // immediate restart rather than leaving the ball dead in the net.
            // Raises OnMatchReset itself, so possession is cleared in lockstep
            // with the ball actually moving back to the centre spot.
            ball.ResetToKickoff();
        }
    }
}
