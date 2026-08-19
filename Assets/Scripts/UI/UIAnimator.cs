using System.Collections;
using UnityEngine;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Fades and scales a panel in and out, so screens arrive instead of
    /// appearing.
    ///
    /// Static helpers rather than a component on each panel. Every caller
    /// already has a <c>uiPanel</c> GameObject reference and already calls
    /// SetActive on it, so a component would mean wiring a second reference on
    /// every screen for something none of them needs to configure. The coroutine
    /// is hosted on a runner of this class's own, which is also what lets a
    /// panel finish fading OUT after its controller has stopped caring.
    ///
    /// Everything runs on unscaled time. Every panel in this game is a modal
    /// that freezes the match at timeScale 0, so a scaled tween would sit at its
    /// first frame forever — the panel would fade to 10% and stay there.
    /// </summary>
    public static class UIAnimator
    {
        public const float DefaultDuration = 0.2f;

        /// <summary>Scale a panel grows from on the way in, and shrinks to on the way out.</summary>
        private const float ClosedScale = 0.9f;

        private static UIAnimatorRunner runner;

        /// <summary>
        /// Shows <paramref name="panel"/> and fades it up from transparent and
        /// slightly small.
        ///
        /// The panel is activated FIRST and its alpha set to 0 in the same frame,
        /// so it is never visible at full opacity for even one frame before the
        /// tween starts.
        /// </summary>
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
                // No CanvasGroup, or we are in the editor where there is nothing
                // to run a coroutine on: the panel still has to appear.
                return;
            }

            group.alpha = 0f;
            panel.transform.localScale = Vector3.one * ClosedScale;

            Run(Tween(panel, group, 0f, 1f, ClosedScale, 1f, duration, deactivateAtEnd: false));
        }

        /// <summary>
        /// Fades <paramref name="panel"/> out and deactivates it when the tween
        /// finishes — not before, or the fade would never be seen.
        /// </summary>
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

            // Blocked as soon as the close begins. The panel is still on screen
            // for the length of the fade, and a button pressed during it would
            // act on a screen the player has already dismissed.
            group.interactable = false;
            group.blocksRaycasts = false;

            Run(Tween(panel, group, group.alpha, 0f, panel.transform.localScale.x, ClosedScale,
                duration, deactivateAtEnd: true));
        }

        private static IEnumerator Tween(GameObject panel, CanvasGroup group,
            float fromAlpha, float toAlpha, float fromScale, float toScale,
            float duration, bool deactivateAtEnd)
        {
            float elapsed = 0f;

            while (elapsed < duration && panel != null && group != null)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / duration);

                // Smoothed rather than linear. Two tenths of a second is short
                // enough that a straight ramp reads as a hard cut with a blur on
                // it; easing is what makes it read as a movement.
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

                // Handed back ready to be shown again. A panel left blocked would
                // open next time with dead buttons.
                group.interactable = true;
                group.blocksRaycasts = true;

                // ...and back at full size, so a caller that shows it without
                // going through Show — an older screen, a test — does not get a
                // panel stuck at 90%.
                panel.transform.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// The panel's CanvasGroup, added on first use if the scene was built
        /// without one. Added rather than required so that a screen the
        /// generator has not been taught about yet still animates instead of
        /// silently doing nothing.
        /// </summary>
        private static CanvasGroup ResolveGroup(GameObject panel)
        {
            if (panel.TryGetComponent(out CanvasGroup group))
            {
                return group;
            }

            return Application.isPlaying ? panel.AddComponent<CanvasGroup>() : null;
        }

        /// <summary>
        /// Runs a tween on a host of this class's own.
        ///
        /// Deliberately not on the calling controller. A panel's fade OUT
        /// outlives the moment its controller stops caring about it, and some of
        /// those controllers deactivate themselves — a coroutine on a disabled
        /// MonoBehaviour stops dead, which would leave the panel half faded and
        /// still on screen.
        /// </summary>
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

    /// <summary>Coroutine host for <see cref="UIAnimator"/>. Holds no state.</summary>
    public class UIAnimatorRunner : MonoBehaviour
    {
    }
}
