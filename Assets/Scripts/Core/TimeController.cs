using UnityEngine;

namespace TacticalSoccer.Core
{
    // Ralentiza el tiempo mientras el jugador dibuja una ruta, y lo restaura al confirmarla.
    public class TimeController : MonoBehaviour
    {
        [SerializeField] private float slowMotionTimeScale = 0.1f;
        [SerializeField] private float normalTimeScale = 1f;
        [SerializeField] private float fixedDeltaTimeAtNormalScale = 0.02f;

        // Se suscribe a los eventos de dibujo de ruta y de balón fuera.
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

        // Pone el juego en cámara lenta.
        private void HandleRouteDrawStarted()
        {
            Time.timeScale = slowMotionTimeScale;
            Time.fixedDeltaTime = fixedDeltaTimeAtNormalScale * Time.timeScale;
        }

        // Restaura la velocidad normal del juego.
        private void HandleRouteDrawEnded()
        {
            Time.timeScale = normalTimeScale;
            Time.fixedDeltaTime = fixedDeltaTimeAtNormalScale;
        }

        // Reacciona a que el balón se ha ido fuera de banda.
        private void HandleBallOutOfBounds()
        {
            Debug.Log("¡Balón fuera de banda! Reseteando posición...");
        }
    }
}
