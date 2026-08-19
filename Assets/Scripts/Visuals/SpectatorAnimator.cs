using UnityEngine;

namespace TacticalSoccer.Visuals
{
    /// <summary>
    /// Makes one spectator bounce in their seat. Nothing but a sine wave on the
    /// local Y — the crowd is scenery, so it never touches physics, never has a
    /// collider, and never needs to know a match is being played in front of it.
    ///
    /// Every value is rolled per spectator in Awake. A shared rhythm would read
    /// as one object duplicated a hundred and seventy times, which looks far
    /// worse than a stand full of people sitting perfectly still.
    ///
    /// Scaled time on purpose: a duel stops the world, and a crowd still
    /// jumping behind a frozen tackle would break the freeze rather than sell it.
    /// </summary>
    public class SpectatorAnimator : MonoBehaviour
    {
        private float bounceSpeed;
        private float bounceHeight;
        private float timeOffset;
        private Vector3 startPos;

        private void Awake()
        {
            startPos = transform.localPosition;

            bounceSpeed = Random.Range(5f, 10f);
            bounceHeight = Random.Range(0.2f, 0.5f);

            // Big enough that no two spectators share a phase, small enough that
            // the sine keeps its resolution.
            timeOffset = Random.Range(0f, 100f);
        }

        private void Update()
        {
            // Clamped at zero so the bounce only ever goes UP: the negative half
            // of the wave would sink each spectator through the step they are
            // standing on.
            float yOffset = Mathf.Max(0f, Mathf.Sin((Time.time * bounceSpeed) + timeOffset) * bounceHeight);

            transform.localPosition = startPos + new Vector3(0f, yOffset, 0f);
        }
    }
}
