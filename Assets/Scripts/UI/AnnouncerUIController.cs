using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Calls the restarts out loud. Set pieces used to happen in silence: the
    /// ball simply reappeared somewhere with somebody standing over it, and
    /// nothing told the player which infringement had been given.
    ///
    /// Holds the message for a beat, then fades it out, so a corner announced
    /// during play does not sit over the pitch for the rest of the match.
    /// </summary>
    public class AnnouncerUIController : MonoBehaviour
    {
        public Text announcerText;

        [Tooltip("How long the message stays at full strength before fading.")]
        [SerializeField] private float holdDuration = 1.5f;

        [Tooltip("How long the fade itself takes.")]
        [SerializeField] private float fadeDuration = 0.5f;

        public static AnnouncerUIController Instance { get; private set; }

        private Coroutine fadeRoutine;

        private void Awake()
        {
            Instance = this;

            SetAlpha(0f);
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Puts a message on screen. Calling it again while one is still showing
        /// replaces it outright — two restarts in quick succession should read as
        /// the latest call, not as a queue.
        /// </summary>
        public void ShowAnnouncement(string message)
        {
            if (announcerText == null)
            {
                return;
            }

            announcerText.text = message;

            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
            }

            fadeRoutine = StartCoroutine(FadeOutText());
        }

        /// <summary>
        /// Everything here runs on unscaled time. A duel freezes the match at
        /// timeScale 0 and drawing a route drops it to 0.1, either of which
        /// would leave an announcement stuck on screen or stretch it out to
        /// fifteen real seconds.
        /// </summary>
        private IEnumerator FadeOutText()
        {
            SetAlpha(1f);

            yield return new WaitForSecondsRealtime(holdDuration);

            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(1f - Mathf.Clamp01(elapsed / fadeDuration));

                yield return null;
            }

            SetAlpha(0f);
            fadeRoutine = null;
        }

        private void SetAlpha(float alpha)
        {
            if (announcerText == null)
            {
                return;
            }

            Color color = announcerText.color;
            color.a = alpha;
            announcerText.color = color;
        }
    }
}
