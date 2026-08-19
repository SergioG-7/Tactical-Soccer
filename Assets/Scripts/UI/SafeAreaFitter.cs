using UnityEngine;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Keeps a stretched RectTransform inside the part of the screen the phone
    /// actually lets you draw on — clear of the notch, the punch-hole camera and
    /// the gesture bar along the bottom.
    ///
    /// Applied to the one container every panel and every HUD element hangs off,
    /// rather than to each of them: the inset is a fact about the DEVICE, not
    /// about any particular screen, and a component per panel would be a dozen
    /// copies of it that can disagree — and would miss whatever screen was added
    /// next.
    ///
    /// It moves the ANCHORS rather than the offsets, and that is not a detail.
    /// <see cref="Screen.safeArea"/> is measured in real screen pixels while a
    /// RectTransform's offsets are in the canvas's reference units, so writing
    /// the inset straight into offsetMin/offsetMax would be out by the
    /// CanvasScaler's factor — about 2.5x on a 1080p phone against a 1920x1080
    /// reference. Anchors are fractions of the parent, which is exactly what a
    /// fraction of the screen is, so they need no conversion and stay right when
    /// the scale factor changes.
    ///
    /// Re-applied when the safe area changes rather than only in Awake: rotating
    /// the device, unfolding a foldable or opening the split-screen shelf all
    /// move it, and a layout computed once would stay wrong for the rest of the
    /// session.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;

        private Rect appliedSafeArea;
        private int appliedWidth;
        private int appliedHeight;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();

            Apply();
        }

        private void Update()
        {
            // Three cheap comparisons a frame. Cheaper than the alternative,
            // which is a screen-orientation callback that Unity only raises on
            // some platforms.
            if (Screen.safeArea == appliedSafeArea
                && Screen.width == appliedWidth
                && Screen.height == appliedHeight)
            {
                return;
            }

            Apply();
        }

        private void Apply()
        {
            if (rect == null)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;

            int width = Screen.width;
            int height = Screen.height;

            // A zero here would divide the layout into infinities. It happens for
            // a frame on some devices while the surface is being created, and on
            // a headless build.
            if (width <= 0 || height <= 0)
            {
                return;
            }

            appliedSafeArea = safeArea;
            appliedWidth = width;
            appliedHeight = height;

            Vector2 min = safeArea.position;
            Vector2 max = safeArea.position + safeArea.size;

            min.x /= width;
            min.y /= height;
            max.x /= width;
            max.y /= height;

            rect.anchorMin = min;
            rect.anchorMax = max;

            // The offsets are zeroed rather than left alone: this rect is a
            // full-bleed container, and any offset serialised onto it would be
            // added on top of the inset we have just worked out.
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
