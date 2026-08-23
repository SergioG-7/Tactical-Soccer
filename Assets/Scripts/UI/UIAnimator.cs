using System.Collections;
using UnityEngine;

namespace TacticalSoccer.UI
{
    // Métodos estáticos para mostrar y ocultar paneles de UI con un fundido y escalado suaves.
    public static class UIAnimator
    {
        public const float DefaultDuration = 0.2f;

        // Escala desde/hacia la que crece o encoge el panel al aparecer o desaparecer.
        private const float ClosedScale = 0.9f;

        private static UIAnimatorRunner runner;

        // Activa el panel y lo hace aparecer con un fundido desde transparente y algo más pequeño.
        public static void Show(GameObject panel, float duration = DefaultDuration)
        {
            if (panel == null)
            {
                return;
            }

            CanvasGroup group = ResolveGroup(panel);

            panel.SetActive(true);

            if (group == null || !Application.isPlaying)
            {
                return;
            }

            group.alpha = 0f;
            panel.transform.localScale = Vector3.one * ClosedScale;

            Run(Tween(panel, group, 0f, 1f, ClosedScale, 1f, duration, deactivateAtEnd: false));
        }

        // Hace desaparecer el panel con un fundido y lo desactiva al terminar.
        public static void Hide(GameObject panel, float duration = DefaultDuration)
        {
            if (panel == null || !panel.activeSelf)
            {
                return;
            }

            CanvasGroup group = ResolveGroup(panel);

            if (group == null || !Application.isPlaying)
            {
                panel.SetActive(false);
                return;
            }

            // Se bloquea la interacción nada más empezar el cierre, aunque el panel siga visible durante el fundido.
            group.interactable = false;
            group.blocksRaycasts = false;

            Run(Tween(panel, group, group.alpha, 0f, panel.transform.localScale.x, ClosedScale,
                duration, deactivateAtEnd: true));
        }

        // Interpola alfa y escala del panel a lo largo de la duración indicada.
        private static IEnumerator Tween(GameObject panel, CanvasGroup group,
            float fromAlpha, float toAlpha, float fromScale, float toScale,
            float duration, bool deactivateAtEnd)
        {
            float elapsed = 0f;

            while (elapsed < duration && panel != null && group != null)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / duration);

                float eased = Mathf.SmoothStep(0f, 1f, t);

                group.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
                panel.transform.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, eased);

                yield return null;
            }

            if (panel == null || group == null)
            {
                yield break;
            }

            group.alpha = toAlpha;
            panel.transform.localScale = Vector3.one * toScale;

            if (deactivateAtEnd)
            {
                panel.SetActive(false);

                // Se deja listo para volver a mostrarse: interactuable y a tamaño completo.
                group.interactable = true;
                group.blocksRaycasts = true;
                panel.transform.localScale = Vector3.one;
            }
        }

        // Devuelve el CanvasGroup del panel, añadiéndolo si no existe.
        private static CanvasGroup ResolveGroup(GameObject panel)
        {
            if (panel.TryGetComponent(out CanvasGroup group))
            {
                return group;
            }

            return Application.isPlaying ? panel.AddComponent<CanvasGroup>() : null;
        }

        // Lanza la corrutina en un host propio, para que sobreviva aunque el controlador que la pidió se desactive.
        private static void Run(IEnumerator routine)
        {
            if (runner == null)
            {
                GameObject host = new GameObject("UIAnimator");
                Object.DontDestroyOnLoad(host);

                runner = host.AddComponent<UIAnimatorRunner>();
            }

            runner.StartCoroutine(routine);
        }
    }

    // Objeto que solo sirve para alojar las corrutinas de UIAnimator.
    public class UIAnimatorRunner : MonoBehaviour
    {
    }
}
