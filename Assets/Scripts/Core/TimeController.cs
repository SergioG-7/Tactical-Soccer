using UnityEngine;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// Listens to TacticalEvents to slow down time while the player draws a
    /// route, and restores normal speed once the route is committed. Holds
    /// no reference to input or player scripts, staying fully decoupled.
    /// </summary>
    public class TimeController : MonoBehaviour
    {
        [Header("Slow-Motion Settings")]
        [SerializeField] private float slowMotionTimeScale = 0.1f;
        [SerializeField] private float normalTimeScale = 1f;
        [SerializeField] private float fixedDeltaTimeAtNormalScale = 0.02f;

        private void OnEnable()
        {
            TacticalEvents.OnRouteDrawStarted += HandleRouteDrawStarted;
            TacticalEvents.OnRouteDrawEnded += HandleRouteDrawEnded;
            TacticalEvents.OnBallOutOfBounds += HandleBallOutOfBounds;
        }

        private void OnDisable()
        {
            TacticalEvents.OnRouteDrawStarted -= HandleRouteDrawStarted;
            TacticalEvents.OnRouteDrawEnded -= HandleRouteDrawEnded;
            TacticalEvents.OnBallOutOfBounds -= HandleBallOutOfBounds;
        }

        private void HandleRouteDrawStarted()
        {
            Time.timeScale = slowMotionTimeScale;
            Time.fixedDeltaTime = fixedDeltaTimeAtNormalScale * Time.timeScale;
        }

        private void HandleRouteDrawEnded()
        {
            Time.timeScale = normalTimeScale;
            Time.fixedDeltaTime = fixedDeltaTimeAtNormalScale;
        }

        /// <summary>
        /// Hook for the upcoming team-repositioning logic; the ball has already
        /// re-centred itself by the time this fires.
        /// </summary>
        private void HandleBallOutOfBounds()
        {
            Debug.Log("¡Balón fuera de banda! Reseteando posición...");
        }
    }
}
