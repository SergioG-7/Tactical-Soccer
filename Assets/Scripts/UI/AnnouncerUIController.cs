using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TacticalSoccer.UI
{
    // Muestra por pantalla el anuncio de los reinicios de juego (faltas, saques, etc.) y lo desvanece tras un rato.
    public class AnnouncerUIController : MonoBehaviour
    {
        public Text announcerText;

        [Tooltip("Tiempo que el mensaje permanece visible antes de empezar a desvanecerse.")]
        [SerializeField] private float holdDuration = 1.5f;

        [Tooltip("Duración de la animación de desvanecimiento (fade out).")]
        [SerializeField] private float fadeDuration = 0.5f;

        public static AnnouncerUIController Instance { get; private set; }

        private Coroutine fadeRoutine;

        // Guarda la instancia y oculta el texto al iniciar.
        private void Awake()
        {
            Instance = this;

            SetAlpha(0f);
        }

        // Limpia la instancia al desactivarse.
        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Muestra un mensaje en pantalla, sustituyendo al anterior si aún está visible.
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

        // Mantiene el texto visible un rato y luego lo desvanece, usando tiempo real (no afectado por pausas ni cámara lenta).
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

        // Ajusta la transparencia del texto del anuncio.
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
